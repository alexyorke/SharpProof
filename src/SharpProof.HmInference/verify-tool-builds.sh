#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
assembly_dir=$(dirname "$output")
evidence=/workspace/SharpProof/artifacts/hm-inference
fixture=$(mktemp -d '/tmp/hm tool dependencies.XXXXXX')
cp global.json "$fixture/global.json"
mkdir -p "$fixture/Generator" "$fixture/Root" "$fixture/feed"
cat > "$fixture/NuGet.Config" <<'EOF'
<configuration><packageSources><clear/><add key="fixture" value="./feed"/></packageSources></configuration>
EOF
cat > "$fixture/Directory.Build.props" <<'EOF'
<Project><PropertyGroup><LangVersion>13.0</LangVersion><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>
EOF
cat > "$fixture/Generator/Generator.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><DefineConstants Condition="'\$(Configuration)'=='Release'">\$(DefineConstants);RELEASE_TOOL</DefineConstants></PropertyGroup><Target Name="RecordBuild" BeforeTargets="Build"><WriteLinesToFile File="../builds.txt" Lines="\$(Configuration):\$(TargetFramework)" Overwrite="false"/></Target><ItemGroup><Reference Include="Microsoft.CodeAnalysis"><HintPath>$assembly_dir/Microsoft.CodeAnalysis.dll</HintPath></Reference></ItemGroup></Project>
EOF
cat > "$fixture/Generator/Generator.cs" <<'EOF'
using Microsoft.CodeAnalysis;
[Generator]public sealed class FixtureGenerator:ISourceGenerator {
    public void Initialize(GeneratorInitializationContext context){}
    public void Execute(GeneratorExecutionContext context){
        var counter=Environment.GetEnvironmentVariable("HM_TOOL_EXECUTIONS");
        if(counter is not null)File.AppendAllText(counter,"execute\n");
#if RELEASE_TOOL
        context.AddSource("Release.cs","public static class ReleaseContract{public const int Value=1;}");
#endif
        context.AddSource("Contract.cs","public static class GeneratedContract{public static T Id<T>(T value)=>value;}");
    }
}
EOF
cat > "$fixture/Root/Root.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><DefineConstants Condition="'$(Configuration)'=='Release'">$(DefineConstants);RELEASE_ROOT</DefineConstants></PropertyGroup><ItemGroup><ProjectReference Include="../Generator/Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" SkipGetTargetFrameworkProperties="true"/></ItemGroup></Project>
EOF
echo 'public static class Root{public static async Task<string?> Identity(Task<string?> value)=>GeneratedContract.Id(await value);}' > "$fixture/Root/Input.cs"
dotnet restore "$fixture/Root/Root.csproj" > "$fixture/restore.log" 2>&1 || { tail -35 "$fixture/restore.log"; exit 1; }
# Query the SDK without compiling either the source root or its references.
dotnet msbuild "$fixture/Root/Root.csproj" -t:ResolveReferences -p:BuildProjectReferences=false -p:DesignTimeBuild=true -p:SkipCompilerExecution=true -getItem:Analyzer -verbosity:quiet > "$fixture/analyzers.json" 2> "$fixture/query-errors.log" || { tail -30 "$fixture/query-errors.log"; tail -20 "$fixture/analyzers.json"; exit 1; }
HM_ANALYZER_JSON="$fixture/analyzers.json" pwsh -NoLogo -NoProfile -Command '$data=Get-Content -LiteralPath $env:HM_ANALYZER_JSON -Raw|ConvertFrom-Json; $tools=@($data.Items.Analyzer|Where-Object {$_.MSBuildSourceProjectFile -like "*/Generator/Generator.csproj"}); if($tools.Count -ne 1 -or !$tools[0].FullPath.EndsWith("/bin/Debug/net10.0/Generator.dll")){throw "Unexpected native tool selection"}'
test ! -e "$fixture/Generator/bin/Debug/net10.0/Generator.dll"
if dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore --no-build > "$fixture/no-build-missing.log" 2>&1; then tail -35 "$fixture/no-build-missing.log"; exit 1; else test "$?" -eq 1; fi
test ! -e "$fixture/Generator/bin/Debug/net10.0/Generator.dll"
if grep -Fq HM0001 "$fixture/no-build-missing.log"; then tail -35 "$fixture/no-build-missing.log"; exit 1; fi
if dotnet "$output" --project "$fixture/Root/Root.csproj" --no-build --no-build > "$fixture/duplicate.log" 2>&1; then exit 1; else test "$?" -eq 2; fi
printf '%s\n' 'PASS CliNoBuildLeavesMissingToolArtifactsUntouched'
HM_TOOL_EXECUTIONS="$fixture/executions.txt" dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/fresh.log" 2>&1 || { tail -40 "$fixture/fresh.log"; exit 1; }
if ! grep -Fq 'Identity<T1>' "$fixture/fresh.log" || ! grep -Fq 'C# project compilations analyzed: 1' "$fixture/fresh.log" || test "$(wc -l < "$fixture/executions.txt")" -ne 1; then tail -40 "$fixture/fresh.log"; exit 1; fi
printf '%s\n' 'PASS CliBuildsDeclaredGeneratorProjectFromCleanCheckout'
cp "$fixture/fresh.log" "$evidence/project-tool-fresh.txt"
# Refresh an existing binary when the generator source changes.
sed -i 's/public static class GeneratedContract{/public static class GeneratedContract{public const int Revision=2;/' "$fixture/Generator/Generator.cs"
cat >> "$fixture/Root/Input.cs" <<'EOF'
public static class RevisionProbe{public static int Revision=>GeneratedContract.Revision;}
#if RELEASE_ROOT
public static class ReleaseProbe{public static int Revision=>ReleaseContract.Value;}
#endif
EOF
if dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore --no-build > "$fixture/stale.log" 2>&1; then tail -35 "$fixture/stale.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq CS0117 "$fixture/stale.log" || grep -Fq HM0001 "$fixture/stale.log"; then tail -35 "$fixture/stale.log"; exit 1; fi
dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/refreshed.log" 2>&1 || { tail -40 "$fixture/refreshed.log"; exit 1; }
if ! grep -Fq 'Identity<T1>' "$fixture/refreshed.log"; then tail -40 "$fixture/refreshed.log"; exit 1; fi
printf '%s\n' 'PASS CliRebuildsStaleGeneratorSource'
dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore --configuration Release > "$fixture/release.log" 2>&1 || { tail -40 "$fixture/release.log"; exit 1; }
if ! grep -Fq 'Identity<T1>' "$fixture/release.log" || ! test -f "$fixture/Generator/bin/Release/net10.0/Generator.dll"; then tail -40 "$fixture/release.log"; exit 1; fi
printf '%s\n' 'PASS CliBuildsGeneratorInSelectedConfiguration'
echo '#error Broken generator source' >> "$fixture/Generator/Generator.cs"
if HM_TOOL_EXECUTIONS="$fixture/failed-executions.txt" dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/failure.log" 2>&1; then tail -40 "$fixture/failure.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq 'Tool dependency build failed' "$fixture/failure.log" || ! grep -Fq CS1029 "$fixture/failure.log" || grep -Fq HM0001 "$fixture/failure.log" || test -e "$fixture/failed-executions.txt"; then tail -40 "$fixture/failure.log"; exit 1; fi
printf '%s\n' 'PASS CliToolBuildFailuresRejectStaleBinaries'
dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore --no-build > "$fixture/cached.log" 2>&1 || { tail -40 "$fixture/cached.log"; exit 1; }
if ! grep -Fq 'Identity<T1>' "$fixture/cached.log"; then tail -40 "$fixture/cached.log"; exit 1; fi
printf '%s\n' 'PASS CliNoBuildUsesSuppliedToolArtifacts'
sed -i '/^#error Broken generator source$/d' "$fixture/Generator/Generator.cs"
mkdir -p "$fixture/Helper" "$fixture/Other"
cat > "$fixture/Helper/Helper.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="../Generator/Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" SkipGetTargetFrameworkProperties="true"/></ItemGroup></Project>
EOF
echo 'public static class Helper{public static T Id<T>(T value)=>GeneratedContract.Id(value);}' > "$fixture/Helper/Input.cs"
sed -i 's#</ItemGroup>#<ProjectReference Include="../Helper/Helper.csproj"/></ItemGroup>#' "$fixture/Root/Root.csproj"
echo 'public static class HelperProbe{public static int Identity(int value)=>Helper.Id(value);}' >> "$fixture/Root/Input.cs"
HM_TOOL_EXECUTIONS="$fixture/ordinary-executions.txt" dotnet "$output" --project "$fixture/Root/Root.csproj" > "$fixture/ordinary.log" 2>&1 || { tail -45 "$fixture/ordinary.log"; exit 1; }
if ! grep -Fq 'C# project compilations analyzed: 2' "$fixture/ordinary.log" || test "$(wc -l < "$fixture/ordinary-executions.txt")" -ne 2; then tail -45 "$fixture/ordinary.log"; cat "$fixture/ordinary-executions.txt"; exit 1; fi
printf '%s\n' 'PASS CliDoesNotBuildOrdinarySourceDependencies'
cat > "$fixture/Other/Other.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="../Generator/Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" SkipGetTargetFrameworkProperties="true"/></ItemGroup></Project>
EOF
echo 'public static class Other{public static int Identity(int value)=>GeneratedContract.Id(value);}' > "$fixture/Other/Input.cs"
cat > "$fixture/Scope.slnx" <<'EOF'
<Solution><Project Path="Root/Root.csproj"/><Project Path="Other/Other.csproj"/></Solution>
EOF
rm -- "$fixture/builds.txt"
HM_TOOL_EXECUTIONS="$fixture/solution-executions.txt" dotnet "$output" --solution "$fixture/Scope.slnx" > "$fixture/solution.log" 2>&1 || { tail -45 "$fixture/solution.log"; exit 1; }
if ! grep -Fq 'C# project compilations analyzed: 3' "$fixture/solution.log" || test "$(wc -l < "$fixture/builds.txt")" -ne 1 || test "$(wc -l < "$fixture/solution-executions.txt")" -ne 3; then tail -45 "$fixture/solution.log"; cat "$fixture/builds.txt"; cat "$fixture/solution-executions.txt"; exit 1; fi
printf '%s\n' 'PASS CliSharedToolContextBuildsOncePerSolution'
cp "$fixture/failure.log" "$evidence/project-tool-failure.txt"
cp "$fixture/ordinary.log" "$evidence/project-tool-source-closure.txt"
cp "$fixture/solution.log" "$evidence/project-tool-solution.txt"
# Tool target selection follows the SDK, including an unusable unselected target.
for scenario in exact nearest; do
  if test "$scenario" = exact; then root_framework=net9.0; tool_frameworks='net9.0;net10.0'; else root_framework=net10.0; tool_frameworks='net9.0;netstandard2.1'; fi
  tool_dir="$fixture/Tool-$scenario"
  root_dir="$fixture/Consumer-$scenario"
  mkdir -p "$tool_dir" "$root_dir"
  cat > "$tool_dir/Tool.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>$tool_frameworks</TargetFrameworks></PropertyGroup><ItemGroup><Reference Include="Microsoft.CodeAnalysis"><HintPath>$assembly_dir/Microsoft.CodeAnalysis.dll</HintPath></Reference></ItemGroup></Project>
EOF
  cp "$fixture/Generator/Generator.cs" "$tool_dir/Generator.cs"
  cat > "$tool_dir/Unavailable.cs" <<'EOF'
#if !NET9_0
#error Unselected generator target must not compile
#endif
EOF
  cat > "$root_dir/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>$root_framework</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="../Tool-$scenario/Tool.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false"/></ItemGroup></Project>
EOF
  echo 'public static class Consumer{public static int Identity(int value)=>GeneratedContract.Id(value);}' > "$root_dir/Input.cs"
  dotnet restore "$root_dir/Consumer.csproj" > "$fixture/$scenario-restore.log" 2>&1 || { tail -35 "$fixture/$scenario-restore.log"; exit 1; }
  dotnet msbuild "$root_dir/Consumer.csproj" -t:ResolveReferences -p:BuildProjectReferences=false -p:DesignTimeBuild=true -p:SkipCompilerExecution=true -getItem:Analyzer -verbosity:quiet > "$fixture/$scenario-query.json" 2>&1 || { tail -35 "$fixture/$scenario-query.json"; exit 1; }
  HM_ANALYZER_JSON="$fixture/$scenario-query.json" pwsh -NoLogo -NoProfile -Command '$data=Get-Content -LiteralPath $env:HM_ANALYZER_JSON -Raw|ConvertFrom-Json; $tools=@($data.Items.Analyzer|Where-Object {$_.MSBuildSourceProjectFile -like "*/Tool.csproj"}); if($tools.Count -ne 1 -or !$tools[0].FullPath.EndsWith("/bin/Debug/net9.0/Tool.dll")){throw "Unexpected native multi-target tool selection"}'
  dotnet "$output" --project "$root_dir/Consumer.csproj" --no-restore > "$fixture/$scenario.log" 2>&1 || { tail -45 "$fixture/$scenario.log"; exit 1; }
  if ! grep -Fq 'Identity<T1>' "$fixture/$scenario.log" || ! grep -Fq 'C# project compilations analyzed: 1' "$fixture/$scenario.log" || ! test -f "$tool_dir/bin/Debug/net9.0/Tool.dll" || test -f "$tool_dir/bin/Debug/net10.0/Tool.dll" || test -f "$tool_dir/bin/Debug/netstandard2.1/Tool.dll"; then tail -45 "$fixture/$scenario.log"; exit 1; fi
  printf '%s\n' "PASS CliBuildsSdkSelectedToolFramework_$scenario"
done
