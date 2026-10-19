#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
evidence=/workspace/SharpProof/artifacts/hm-inference
fixture=$(mktemp -d '/tmp/hm framework closure.XXXXXX')
cp global.json "$fixture/global.json"
mkdir -p "$fixture/Root8" "$fixture/Root9" "$fixture/Dependency" "$fixture/feed"
cat > "$fixture/NuGet.Config" <<'EOF'
<configuration><packageSources><clear/><add key="fixture" value="./feed"/></packageSources></configuration>
EOF
cat > "$fixture/Directory.Build.props" <<'EOF'
<Project><PropertyGroup><LangVersion>13.0</LangVersion><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
EOF
cat > "$fixture/Dependency/Dependency.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup></Project>
EOF
cat > "$fixture/Dependency/Contract.cs" <<'EOF'
#if NET10_0
#error Deliberately broken unselected dependency framework
#endif
public static class FrameworkContract {public static T Identity<T>(T value)=>value;public static string DependencyIdentity(string value)=>value;}
EOF
for framework in 8 9; do
cat > "$fixture/Root$framework/Root$framework.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net$framework.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="../Dependency/Dependency.csproj"/></ItemGroup></Project>
EOF
cat > "$fixture/Root$framework/Root.cs" <<'EOF'
public static class Root {public static async System.Threading.Tasks.Task<string?> NullableAsync(System.Threading.Tasks.Task<string?> value)=>FrameworkContract.Identity(await value);}
EOF
done
# Establish the expected compiler-selected closure independently of the analyzer.
dotnet build "$fixture/Root8/Root8.csproj" > "$fixture/native8.log" 2>&1 || { tail -35 "$fixture/native8.log"; exit 1; }
dotnet "$output" --project "$fixture/Root8/Root8.csproj" --no-restore > "$fixture/exact.log" 2>&1 || { tail -40 "$fixture/exact.log"; exit 1; }
if ! grep -Fq 'NullableAsync<T1>' "$fixture/exact.log" || ! grep -Fq 'DependencyIdentity<T1>' "$fixture/exact.log" || ! grep -Fq 'C# project compilations analyzed: 2' "$fixture/exact.log"; then tail -40 "$fixture/exact.log"; exit 1; fi
printf '%s\n' 'PASS CliPrunesUnselectedReferenceFramework'
dotnet build "$fixture/Root9/Root9.csproj" > "$fixture/native9.log" 2>&1 || { tail -35 "$fixture/native9.log"; exit 1; }
dotnet "$output" --project "$fixture/Root9/Root9.csproj" --no-restore > "$fixture/nearest.log" 2>&1 || { tail -40 "$fixture/nearest.log"; exit 1; }
if ! grep -Fq 'NullableAsync<T1>' "$fixture/nearest.log" || ! grep -Fq 'C# project compilations analyzed: 2' "$fixture/nearest.log"; then tail -40 "$fixture/nearest.log"; exit 1; fi
printf '%s\n' 'PASS CliNearestCompatibleReferenceFramework'
# An explicitly requested framework remains in scope even if no caller selects it.
if dotnet "$output" --project "$fixture/Dependency/Dependency.csproj" > "$fixture/explicit.log" 2>&1; then tail -40 "$fixture/explicit.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq 'Deliberately broken unselected dependency framework' "$fixture/explicit.log" || grep -Fq 'HM0001' "$fixture/explicit.log"; then tail -40 "$fixture/explicit.log"; exit 1; fi
printf '%s\n' 'PASS CliExplicitMultiTargetRootsRetainAllFrameworks'
cp "$fixture/exact.log" "$evidence/project-framework-closure.txt"
cp "$fixture/nearest.log" "$evidence/project-framework-nearest.txt"
(cd "$fixture" && dotnet new sln --format sln -n Scope > /dev/null && dotnet sln Scope.sln add Root8/Root8.csproj Dependency/Dependency.csproj > /dev/null)
if dotnet "$output" --solution "$fixture/Scope.sln" > "$fixture/solution.log" 2>&1; then tail -40 "$fixture/solution.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq 'Deliberately broken unselected dependency framework' "$fixture/solution.log" || grep -Fq 'HM0001' "$fixture/solution.log"; then tail -40 "$fixture/solution.log"; exit 1; fi
printf '%s\n' 'PASS CliSolutionExplicitRootsRetainAllFrameworks'
cat > "$fixture/Scope.slnf" <<'EOF'
{"solution":{"path":"Scope.sln","projects":["Root8/Root8.csproj"]}}
EOF
dotnet "$output" --solution "$fixture/Scope.slnf" > "$fixture/filter.log" 2>&1 || { tail -40 "$fixture/filter.log"; exit 1; }
if ! grep -Fq 'NullableAsync<T1>' "$fixture/filter.log" || ! grep -Fq 'C# project compilations analyzed: 2' "$fixture/filter.log"; then tail -40 "$fixture/filter.log"; exit 1; fi
printf '%s\n' 'PASS CliFilteredSolutionPrunesUnselectedReferenceFramework'
cp "$fixture/filter.log" "$evidence/project-framework-filter.txt"
