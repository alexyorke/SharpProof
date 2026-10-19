#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
fixture=$(mktemp -d '/tmp/hm unsafe compilation.XXXXXX')
cp global.json "$fixture/global.json"
mkdir -p "$fixture/feed"
cat > "$fixture/NuGet.Config" <<'EOF'
<configuration><packageSources><clear/><add key="fixture" value="./feed"/></packageSources></configuration>
EOF
cat > "$fixture/Input.cs" <<'EOF'
public class CliPin<TTag,TValue>{public TValue Value=default!;public ref TValue GetPinnableReference()=>ref Value;}
public unsafe struct CliBuffer<TTag>{public fixed int Values[4];public TTag Tag;}
public unsafe static class UnsafeCli {
    public static int Pin(CliPin<int,int> value){fixed(int* pointer=value){return *pointer;}}
    public static int Buffer(ref CliBuffer<int> value){fixed(int* pointer=value.Values){return pointer[0];}}
    public static int Read(int* value)=>*value;
    public static int Stack(int value){var buffer=stackalloc[]{value};return buffer[0];}
    public static int Indirect(delegate*<int,int> pointer,int value)=>pointer(value);
    public static int Native(delegate* unmanaged[Cdecl]<int,int> pointer,int value)=>pointer(value);
}
EOF
if dotnet "$output" "$fixture/Input.cs" > "$fixture/disabled.log" 2>&1; then tail -35 "$fixture/disabled.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq CS0227 "$fixture/disabled.log" || grep -Fq HM0001 "$fixture/disabled.log"; then tail -35 "$fixture/disabled.log"; exit 1; fi
dotnet "$output" --unsafe "$fixture/Input.cs" > "$fixture/enabled.log" 2>&1 || { tail -35 "$fixture/enabled.log"; exit 1; }
for expected in 'Pin<T1>(global::CliPin<T1, int> value)' 'Buffer<T1>(ref global::CliBuffer<T1> value)' 'Read<T1>(T1* value) where T1 : unmanaged' 'Stack<T1>(T1 value) where T1 : unmanaged' 'Indirect<T1, T2>(delegate*<T1, T2> pointer, T1 value)' 'Native<T1, T2>(delegate* unmanaged[Cdecl]<T1, T2> pointer, T1 value)'; do
  if ! grep -Fq "$expected" "$fixture/enabled.log"; then tail -35 "$fixture/enabled.log"; exit 1; fi
done
if dotnet "$output" --unsafe --unsafe "$fixture/Input.cs" > "$fixture/duplicate.log" 2>&1; then exit 1; else test "$?" -eq 2; fi
printf '%s\n' 'PASS CliExplicitUnsafeSourceCompilation'
cat > "$fixture/Unsafe.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><NuGetAudit>false</NuGetAudit><AllowUnsafeBlocks>false</AllowUnsafeBlocks></PropertyGroup></Project>
EOF
if dotnet "$output" --project "$fixture/Unsafe.csproj" > "$fixture/project-disabled.log" 2>&1; then tail -35 "$fixture/project-disabled.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq CS0227 "$fixture/project-disabled.log" || grep -Fq HM0001 "$fixture/project-disabled.log"; then tail -35 "$fixture/project-disabled.log"; exit 1; fi
dotnet "$output" --project "$fixture/Unsafe.csproj" --property AllowUnsafeBlocks=true --no-restore > "$fixture/project-enabled.log" 2>&1 || { tail -35 "$fixture/project-enabled.log"; exit 1; }
if ! grep -Fq 'Read<T1>(T1* value) where T1 : unmanaged' "$fixture/project-enabled.log"; then tail -35 "$fixture/project-enabled.log"; exit 1; fi
if ! grep -Fq 'Indirect<T1, T2>(delegate*<T1, T2> pointer, T1 value)' "$fixture/project-enabled.log"; then tail -35 "$fixture/project-enabled.log"; exit 1; fi
printf '%s\n' 'PASS CliProjectUnsafeSettingsAndPointerSuggestions'
for expected in 'Pin<T1>(global::CliPin<T1, int> value)' 'Buffer<T1>(ref global::CliBuffer<T1> value)'; do
  if ! grep -Fq "$expected" "$fixture/project-enabled.log"; then tail -35 "$fixture/project-enabled.log"; exit 1; fi
done
printf '%s\n' 'PASS CliPinningPatternsAndFixedBuffers'
cp "$fixture/enabled.log" /workspace/SharpProof/artifacts/hm-inference/unsafe-cli.txt
cp "$fixture/project-enabled.log" /workspace/SharpProof/artifacts/hm-inference/unsafe-project.txt
