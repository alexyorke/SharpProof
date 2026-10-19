#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
evidence=/workspace/SharpProof/artifacts/hm-inference
fixture=$(mktemp -d '/tmp/hm project fixture.XXXXXX')
cp global.json "$fixture/global.json"
mkdir -p "$fixture/App" "$fixture/Library" "$fixture/VisualBasic" "$fixture/Generator" "$fixture/feed"
cat > "$fixture/NuGet.Config" <<'EOF'
<configuration><packageSources><clear/><add key="fixture" value="./feed"/></packageSources></configuration>
EOF
cat > "$fixture/Directory.Build.props" <<'EOF'
<Project><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion Condition="'$(MSBuildProjectExtension)'=='.csproj'">13.0</LangVersion><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><DefineConstants Condition="'$(MSBuildProjectExtension)'=='.csproj'">$(DefineConstants);PROJECT_INFERENCE</DefineConstants><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
EOF
cat > "$fixture/Library/Library.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Compile Include="Library.cs" /></ItemGroup></Project>
EOF
cat > "$fixture/Library/Library.cs" <<'EOF'
namespace Domain;public sealed class Box<T>(T value){public T Value=>value;}public static class Contract{public static T Id<T>(T value)=>value;}
EOF
cat > "$fixture/VisualBasic/VisualBasic.vbproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><RootNamespace>DomainVb</RootNamespace><TargetFrameworks>net8.0;net10.0</TargetFrameworks><TargetFramework></TargetFramework></PropertyGroup><ItemGroup><Compile Include="Contract.vb" /><ProjectReference Include="../Library/Library.csproj"/></ItemGroup></Project>
EOF
cat > "$fixture/VisualBasic/Contract.vb" <<'EOF'
#If NET8_0 Or NET10_0 Then
Public Class VbContract
    Public Shared Function Echo(Of T)(value As T) As T
        Return Global.Domain.Contract.Id(value)
    End Function
End Class
#End If
EOF
dotnet build "$fixture/VisualBasic/VisualBasic.vbproj" > "$fixture/vb-build.log" 2>&1 || { tail -40 "$fixture/vb-build.log"; exit 1; }
assembly_dir=$(dirname "$output")
cat > "$fixture/Generator/Generator.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="Generator.cs"/><Reference Include="Microsoft.CodeAnalysis"><HintPath>$assembly_dir/Microsoft.CodeAnalysis.dll</HintPath></Reference><Reference Include="Microsoft.CodeAnalysis.CSharp"><HintPath>$assembly_dir/Microsoft.CodeAnalysis.CSharp.dll</HintPath></Reference></ItemGroup></Project>
EOF
cat > "$fixture/Generator/Generator.cs" <<'EOF'
using Microsoft.CodeAnalysis;
[Generator] public sealed class FixtureGenerator:ISourceGenerator {
    public void Initialize(GeneratorInitializationContext context){}
    public void Execute(GeneratorExecutionContext context){
        context.AddSource("GeneratedApi.g.cs",context.AdditionalFiles.Single().GetText(context.CancellationToken)!);
        if(context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.CustomSetting",out var setting)&&setting=="enabled")
            context.AddSource("CustomGeneratedApi.g.cs","public static class CustomGeneratedApi{public static T Id<T>(T value)=>value;}");
    }
}
EOF
echo 'public static class GeneratedApi{public static T Id<T>(T value)=>value;}' > "$fixture/GeneratedInput.txt"
dotnet build "$fixture/Generator/Generator.csproj" > "$fixture/generator.log" 2>&1 || { tail -40 "$fixture/generator.log"; exit 1; }
cat > "$fixture/Shared.cs" <<'EOF'
public static class LinkedCode{public static string LinkedIdentity(string value)=>value;}
EOF
cat > "$fixture/App/App.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks><TargetFramework></TargetFramework><DefineConstants Condition="'$(Configuration)'=='Release'">$(DefineConstants);RELEASE_PROJECT</DefineConstants><DefineConstants Condition="'$(CustomSetting)'=='enabled'">$(DefineConstants);CUSTOM_PROJECT</DefineConstants><DefineConstants Condition="'$(EscapedSetting)'=='first;comma,equals=ok'">$(DefineConstants);ESCAPED_SETTING</DefineConstants></PropertyGroup><ItemGroup><ProjectReference Include="../Library/Library.csproj"/><ProjectReference Include="../VisualBasic/VisualBasic.vbproj" Aliases="global,VbAlias"/><Analyzer Include="../Generator/bin/Debug/net10.0/Generator.dll"/><AdditionalFiles Include="../GeneratedInput.txt"/><CompilerVisibleProperty Include="CustomSetting"/><Compile Include="App.cs"/><Compile Include="../Shared.cs" Link="Linked.cs"/></ItemGroup></Project>
EOF
cat > "$fixture/App/App.cs" <<'EOF'
extern alias VbAlias;
using Domain;
public ref struct ProjectToken {}
public static class Consumer {
#if PROJECT_INFERENCE
    public static int ProjectIdentity(int value)=>Contract.Id(value);
    public static ProjectToken ProjectRefLike(ProjectToken value,int ignored)=>value;
    public static int VisualBasicIdentity(int value)=>VbAlias::DomainVb.VbContract.Echo(value);
    public static int GeneratedIdentity(int value)=>GeneratedApi.Id(value);
#if RELEASE_PROJECT && CUSTOM_PROJECT
    public static int PropertyIdentity(int value)=>CustomGeneratedApi.Id(value);
#endif
#if ESCAPED_SETTING
    public static int EscapedIdentity(int value)=>value;
#endif
    public static int ProjectPayload(Box<int> box)=>box.Value;
    public static async Task<int?> ProjectNullableAsync(Task<int?> value)=>await value;
    public static async Task<string?> ProjectNullableReferenceAsync(Task<string?> value)=>await value;
#else
#error Project preprocessor constants were lost
#endif
#if NET10_0
    public static int Net10Identity(int value)=>value;
#else
    public static string Net8Identity(string value)=>value;
#endif
}
EOF
# An excluded file must not enter compilation through directory enumeration.
echo '#error Excluded file was loaded' > "$fixture/App/Excluded.cs"
dotnet restore "$fixture/App/App.csproj" > "$fixture/restore.log" 2>&1 || { tail -40 "$fixture/restore.log"; exit 1; }
dotnet "$output" --project "$fixture/App/App.csproj" > "$fixture/project.log" 2>&1 || { tail -50 "$fixture/project.log"; exit 1; }
for expected in 'ProjectIdentity<T1>' 'ProjectPayload<T1>' 'ProjectNullableAsync<T1>' 'ProjectNullableReferenceAsync<T1>' 'LinkedIdentity<T1>' 'Net8Identity<T1>' 'Net10Identity<T1>' 'VisualBasicIdentity<T1>' 'GeneratedIdentity<T1>'; do
  if ! grep -Fq "$expected" "$fixture/project.log"; then tail -50 "$fixture/project.log"; exit 1; fi
done
printf '%s\n' 'PASS CliMSBuildReferencesSourcesNullableAndAllFrameworks'
dotnet "$output" "$fixture/App/App.csproj" --framework net8.0 --configuration Release --property CustomSetting=enabled --no-restore > "$fixture/framework.log" 2>&1 || { tail -50 "$fixture/framework.log"; exit 1; }
if ! grep -Fq 'Net8Identity<T1>' "$fixture/framework.log" || ! grep -Fq 'PropertyIdentity<T1>' "$fixture/framework.log" || grep -Fq 'Net10Identity<T1>' "$fixture/framework.log"; then tail -50 "$fixture/framework.log"; exit 1; fi
if ! grep -Fq 'global::ProjectToken ProjectRefLike<T1>(global::ProjectToken value, T1 ignored)' "$fixture/framework.log"; then tail -50 "$fixture/framework.log"; exit 1; fi
printf '%s\n' 'PASS CliExplicitFrameworkAndProperties'
if ! grep -Fq 'Task<T1?> ProjectNullableAsync<T1>(global::System.Threading.Tasks.Task<T1?> value) where T1 : struct' "$fixture/framework.log" || ! grep -Fq 'Task<T1?> ProjectNullableReferenceAsync<T1>(global::System.Threading.Tasks.Task<T1?> value) where T1 : class' "$fixture/framework.log"; then tail -50 "$fixture/framework.log"; exit 1; fi
printf '%s\n' 'PASS CliNullableAsyncValueAndReferenceConstraints'
dotnet "$output" --project "$fixture/App/App.csproj" --framework net8.0 --property 'EscapedSetting=first;comma,equals=ok' > "$fixture/escaped.log" 2>&1 || { tail -50 "$fixture/escaped.log"; exit 1; }
if ! grep -Fq 'EscapedIdentity<T1>' "$fixture/escaped.log"; then tail -50 "$fixture/escaped.log"; exit 1; fi
printf '%s\n' 'PASS CliEscapedMSBuildPropertyValues'
(cd "$fixture" && dotnet new sln --format sln -n Fixture > /dev/null && dotnet sln Fixture.sln add App/App.csproj Library/Library.csproj VisualBasic/VisualBasic.vbproj > /dev/null)
dotnet "$output" --solution "$fixture/Fixture.sln" > "$fixture/solution.log" 2>&1 || { tail -50 "$fixture/solution.log"; exit 1; }
if ! grep -Fq 'ProjectIdentity<T1>' "$fixture/solution.log" || ! grep -Fq 'LinkedIdentity<T1>' "$fixture/solution.log"; then tail -50 "$fixture/solution.log"; exit 1; fi
printf '%s\n' 'PASS CliSolutionGraph'

(cd "$fixture" && dotnet new sln --format slnx -n FixtureXml > /dev/null && dotnet sln FixtureXml.slnx add App/App.csproj Library/Library.csproj VisualBasic/VisualBasic.vbproj > /dev/null)
dotnet "$output" --solution "$fixture/FixtureXml.slnx" > "$fixture/slnx.log" 2>&1 || { tail -50 "$fixture/slnx.log"; exit 1; }
if ! grep -Fq 'ProjectIdentity<T1>' "$fixture/slnx.log"; then tail -50 "$fixture/slnx.log"; exit 1; fi
printf '%s\n' 'PASS CliXmlSolutionAndVisualBasicReference'
cat > "$fixture/Fixture.slnf" <<'EOF'
{"solution":{"path":"Fixture.sln","projects":["App/App.csproj"]}}
EOF
dotnet "$output" --solution "$fixture/Fixture.slnf" > "$fixture/filter.log" 2>&1 || { tail -50 "$fixture/filter.log"; exit 1; }
if ! grep -Fq 'ProjectIdentity<T1>' "$fixture/filter.log"; then tail -50 "$fixture/filter.log"; exit 1; fi
printf '%s\n' 'PASS CliSolutionFilter'
cat > "$fixture/App/.editorconfig" <<'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=none
EOF
dotnet "$output" --project "$fixture/App/App.csproj" --framework net8.0 > "$fixture/suppressed.log" 2>&1 || { tail -50 "$fixture/suppressed.log"; exit 1; }
if grep -Eq 'ProjectIdentity<T1>|ProjectPayload<T1>|ProjectNullableAsync<T1>' "$fixture/suppressed.log"; then tail -50 "$fixture/suppressed.log"; exit 1; fi
printf '%s\n' 'PASS CliEditorConfigSuppression'
if dotnet "$output" --project "$fixture/App/App.csproj" --framework > "$fixture/invalid.log" 2>&1; then cat "$fixture/invalid.log"; exit 1; else test "$?" -eq 2; fi
if dotnet "$output" --project "$fixture/App/App.csproj" --property invalid > "$fixture/invalid.log" 2>&1; then cat "$fixture/invalid.log"; exit 1; else test "$?" -eq 2; fi
if dotnet "$output" --project "$fixture/App/App.csproj" --property 'Bad;Property=enabled' > "$fixture/invalid-name.log" 2>&1; then cat "$fixture/invalid-name.log"; exit 1; else test "$?" -eq 2; fi
printf '%s\n' 'PASS CliInvalidProjectOptions'
echo 'invalid Visual Basic declaration' >> "$fixture/VisualBasic/Contract.vb"
if dotnet "$output" --project "$fixture/App/App.csproj" --framework net8.0 > "$fixture/vb-failure.log" 2>&1; then tail -30 "$fixture/vb-failure.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Eq 'error BC[0-9]+' "$fixture/vb-failure.log"; then tail -30 "$fixture/vb-failure.log"; exit 1; fi
printf '%s\n' 'PASS CliReferenceSourceErrorsOverrideStaleBinaries'
mkdir -p "$fixture/Broken" "$fixture/Unrestored"
cat > "$fixture/Broken/Broken.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Compile Include="Broken.cs" /></ItemGroup></Project>
EOF
echo '#error Deliberate compilation failure' > "$fixture/Broken/Broken.cs"
dotnet restore "$fixture/Broken/Broken.csproj" > "$fixture/broken-restore.log" 2>&1 || { tail -40 "$fixture/broken-restore.log"; exit 1; }
if dotnet "$output" --project "$fixture/Broken/Broken.csproj" > "$fixture/broken.log" 2>&1; then tail -30 "$fixture/broken.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq 'inference skipped' "$fixture/broken.log" || grep -q 'HM0001' "$fixture/broken.log"; then tail -30 "$fixture/broken.log"; exit 1; fi
cat > "$fixture/Unrestored/Unrestored.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Compile Include="Unrestored.cs" /><PackageReference Include="Missing.HmInference.Dependency" Version="1.0.0"/></ItemGroup></Project>
EOF
echo 'public static class Unrestored {public static int Identity(int value)=>value;}' > "$fixture/Unrestored/Unrestored.cs"
if dotnet "$output" --project "$fixture/Unrestored/Unrestored.csproj" > "$fixture/unrestored.log" 2>&1; then tail -30 "$fixture/unrestored.log"; exit 1; else test "$?" -eq 1; fi
if grep -q 'HM0001' "$fixture/unrestored.log"; then tail -30 "$fixture/unrestored.log"; exit 1; fi
printf '%s\n' 'PASS CliCompilationAndLoadFailureGates'
cp "$fixture/project.log" "$evidence/project-consumer.txt"
cp "$fixture/solution.log" "$evidence/solution-consumer.txt"
dotnet "$output" --project src/SharpProof.HmInference.Core/SharpProof.HmInference.Core.csproj > "$fixture/repository.log" 2>&1 || { tail -50 "$fixture/repository.log"; exit 1; }
if ! grep -Fq 'C# project compilations analyzed: 2' "$fixture/repository.log"; then tail -50 "$fixture/repository.log"; exit 1; fi
if ! grep -Fq 'Project: SharpProof.HmInference.Core ' "$fixture/repository.log" || ! grep -Fq 'Project: SharpProof.HmInference.Analyzer ' "$fixture/repository.log" || grep -Eq '^Project: (ConsoleLoggingAnalyzer|DefensiveCodingAnalyzer) ' "$fixture/repository.log"; then tail -50 "$fixture/repository.log"; exit 1; fi
cp "$fixture/repository.log" "$evidence/project-repository.txt"
printf '%s\n' 'PASS CliRepositoryArcadeProject'

# Solution mappings and ProjectReference global properties must select source contexts.
mkdir -p "$fixture/Contexts" "$fixture/ContextRoot"
cat > "$fixture/Contexts/Contexts.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><DefineConstants Condition="'$(Configuration)'=='Mapped'">$(DefineConstants);MAPPED_CONTEXT</DefineConstants></PropertyGroup><ItemGroup><Compile Include="Contexts.cs"/></ItemGroup></Project>
EOF
cat > "$fixture/Contexts/Contexts.cs" <<'EOF'
#if MAPPED_CONTEXT
public static class MappedApi {public static T Identity<T>(T value)=>value;public static async System.Threading.Tasks.Task<string?> MappedNullableAsync(System.Threading.Tasks.Task<string?> value)=>await value;}
#else
#error Incorrect mapped configuration
#endif
EOF
(cd "$fixture" && dotnet new sln --format sln -n Mapped > /dev/null && dotnet sln Mapped.sln add Contexts/Contexts.csproj > /dev/null)
sed -i -e 's/\.Debug|Any CPU\.ActiveCfg = Debug|Any CPU/.Debug|Any CPU.ActiveCfg = Mapped|Any CPU/' -e 's/\.Debug|Any CPU\.Build.0 = Debug|Any CPU/.Debug|Any CPU.Build.0 = Mapped|Any CPU/' "$fixture/Mapped.sln"
dotnet "$output" --solution "$fixture/Mapped.sln" > "$fixture/mapped.log" 2>&1 || { tail -50 "$fixture/mapped.log"; exit 1; }
if ! grep -Fq 'MappedNullableAsync<T1>' "$fixture/mapped.log"; then tail -50 "$fixture/mapped.log"; exit 1; fi
printf '%s\n' 'PASS CliSolutionConfigurationMappings'
cat > "$fixture/ContextRoot/ContextRoot.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Compile Include="ContextRoot.cs"/><ProjectReference Include="../Contexts/Contexts.csproj" AdditionalProperties="Configuration=Mapped"/></ItemGroup></Project>
EOF
cat > "$fixture/ContextRoot/ContextRoot.cs" <<'EOF'
public static class ContextRoot {public static async System.Threading.Tasks.Task<string?> ReferenceNullableAsync(System.Threading.Tasks.Task<string?> value)=>MappedApi.Identity(await value);}
EOF
dotnet "$output" --project "$fixture/ContextRoot/ContextRoot.csproj" > "$fixture/reference-context.log" 2>&1 || { tail -50 "$fixture/reference-context.log"; exit 1; }
if ! grep -Fq 'ReferenceNullableAsync<T1>' "$fixture/reference-context.log"; then tail -50 "$fixture/reference-context.log"; exit 1; fi
printf '%s\n' 'PASS CliReferenceConfigurationProperties'
cp "$fixture/mapped.log" "$evidence/project-mapped-context.txt"
cp "$fixture/reference-context.log" "$evidence/project-reference-context.txt"
