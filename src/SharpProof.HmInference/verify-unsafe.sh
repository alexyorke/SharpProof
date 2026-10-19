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
public unsafe static class UnsafeCli {
    public static int Read(int* value)=>*value;
    public static int Stack(int value){var buffer=stackalloc[]{value};return buffer[0];}
}
EOF
if dotnet "$output" "$fixture/Input.cs" > "$fixture/disabled.log" 2>&1; then tail -35 "$fixture/disabled.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq CS0227 "$fixture/disabled.log" || grep -Fq HM0001 "$fixture/disabled.log"; then tail -35 "$fixture/disabled.log"; exit 1; fi
dotnet "$output" --unsafe "$fixture/Input.cs" > "$fixture/enabled.log" 2>&1 || { tail -35 "$fixture/enabled.log"; exit 1; }
for expected in 'Read<T1>(T1* value) where T1 : unmanaged' 'Stack<T1>(T1 value) where T1 : unmanaged'; do
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
printf '%s\n' 'PASS CliProjectUnsafeSettingsAndPointerSuggestions'
cp "$fixture/enabled.log" /workspace/SharpProof/artifacts/hm-inference/unsafe-cli.txt
cp "$fixture/project-enabled.log" /workspace/SharpProof/artifacts/hm-inference/unsafe-project.txt
