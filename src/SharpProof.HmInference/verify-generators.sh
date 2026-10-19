#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
assembly_dir=$(dirname "$output")
evidence=/workspace/SharpProof/artifacts/hm-inference
fixture=$(mktemp -d '/tmp/hm generator diagnostics.XXXXXX')
cp global.json "$fixture/global.json"
mkdir -p "$fixture/Generator" "$fixture/Root" "$fixture/feed"
cat > "$fixture/NuGet.Config" <<'EOF'
<configuration><packageSources><clear/><add key="fixture" value="./feed"/></packageSources></configuration>
EOF
cat > "$fixture/Directory.Build.props" <<'EOF'
<Project><PropertyGroup><LangVersion Condition="'$(MSBuildProjectExtension)'=='.csproj'">13.0</LangVersion><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>
EOF
cat > "$fixture/Generator/Generator.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Reference Include="Microsoft.CodeAnalysis"><HintPath>$assembly_dir/Microsoft.CodeAnalysis.dll</HintPath></Reference><Reference Include="Microsoft.CodeAnalysis.CSharp"><HintPath>$assembly_dir/Microsoft.CodeAnalysis.CSharp.dll</HintPath></Reference></ItemGroup></Project>
EOF
cat > "$fixture/Generator/Generator.cs" <<'EOF'
using Microsoft.CodeAnalysis;
[Generator(LanguageNames.CSharp,LanguageNames.VisualBasic)]
public sealed class FixtureGenerator:ISourceGenerator {
    public FixtureGenerator(){if(Environment.GetEnvironmentVariable("HM_GENERATOR_CTOR_FAIL")=="1")throw new InvalidOperationException("Constructor fixture failure");}
    public void Initialize(GeneratorInitializationContext context){if(Environment.GetEnvironmentVariable("HM_GENERATOR_INIT_FAIL")=="1")throw new InvalidOperationException("Initialization fixture failure");}
    public void Execute(GeneratorExecutionContext context){
        var mode=context.AdditionalFiles.Single().GetText(context.CancellationToken)!.ToString().Trim();
        var counter=Environment.GetEnvironmentVariable("HM_GENERATOR_COUNTER");
        if(counter is not null)File.AppendAllText(counter,"execute\n");
        if(mode=="throw")throw new InvalidOperationException("Execution fixture failure");
        if(mode=="error"||mode=="warn")context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(mode=="error"?"HMG001":"HMG002","Fixture","Generator fixture diagnostic","Fixture",mode=="error"?DiagnosticSeverity.Error:DiagnosticSeverity.Warning,true),Location.None));
        if(mode=="postinit-warning")context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("HMG003","Fixture","Post-initialization diagnostic","Fixture",DiagnosticSeverity.Warning,true),context.Compilation.GetTypeByMetadataName("PostContract")!.Locations.Single(location=>location.IsInSource)));
        if(mode=="generated-warning")context.AddSource("GeneratedWarning.cs","#nullable enable\npublic static class GeneratedWarnings {public static string Bad()=>null;}");
        if(context.Compilation.Language==LanguageNames.CSharp)
            context.AddSource("GeneratedPlain.cs","public static class GeneratedContract {public static T Id<T>(T value)=>value;public static int GeneratedIdentity(int value)=>value;}");
        else context.AddSource("GeneratedPlain.vb","Public Module GeneratedContract\nPublic Function Id(Of T)(value As T) As T\nReturn value\nEnd Function\nEnd Module");
    }
}
[Generator]
public sealed class IncrementalFixture:IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext context){
        context.RegisterPostInitializationOutput(output=>output.AddSource("PostInitialization.cs","public static class PostContract {public static T Id<T>(T value)=>value;}"));
        context.RegisterSourceOutput(context.AdditionalTextsProvider.Collect(),(output,inputs)=>{if(inputs.Single().GetText(output.CancellationToken)!.ToString().Trim()=="incremental-throw")throw new InvalidOperationException("Incremental fixture failure");output.AddSource("IncrementalPlain.cs","public static class IncrementalContract {public static T Id<T>(T value)=>value;}");});
    }
}
EOF
dotnet build "$fixture/Generator/Generator.csproj" > "$fixture/generator-build.log" 2>&1 || { tail -35 "$fixture/generator-build.log"; exit 1; }
cat > "$fixture/Root/Root.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><DefineConstants Condition="'$(NeedGenerated)'=='true'">$(DefineConstants);NEED_GENERATED</DefineConstants></PropertyGroup><ItemGroup><Analyzer Include="../Generator/bin/Debug/net10.0/Generator.dll"/><AdditionalFiles Include="mode.txt"/></ItemGroup></Project>
EOF
cat > "$fixture/Root/Root.cs" <<'EOF'
public static class Root {
    public static int Identity(int value)=>value;
#if NEED_GENERATED
    public static async Task<string?> NullableAsync(Task<string?> value)=>PostContract.Id(IncrementalContract.Id(GeneratedContract.Id(await value)));
#endif
}
EOF
echo ok > "$fixture/Root/mode.txt"
HM_GENERATOR_COUNTER="$fixture/executions.txt" dotnet "$output" --project "$fixture/Root/Root.csproj" --property NeedGenerated=true > "$fixture/success.log" 2>&1 || { tail -40 "$fixture/success.log"; exit 1; }
if ! grep -Fq 'NullableAsync<T1>' "$fixture/success.log" || ! grep -Fq 'Identity<T1>' "$fixture/success.log" || test "$(wc -l < "$fixture/executions.txt")" -ne 1; then tail -40 "$fixture/success.log"; cat "$fixture/executions.txt"; exit 1; fi
printf '%s\n' 'PASS CliRunsClassicIncrementalAndPostInitGeneratorsOnce'
echo error > "$fixture/Root/mode.txt"
if dotnet build "$fixture/Root/Root.csproj" > "$fixture/native-error.log" 2>&1; then tail -35 "$fixture/native-error.log"; exit 1; fi
if ! grep -Fq 'error HMG001' "$fixture/native-error.log"; then tail -35 "$fixture/native-error.log"; exit 1; fi
if dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/error.log" 2>&1; then tail -40 "$fixture/error.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq HMG001 "$fixture/error.log" || grep -Fq HM0001 "$fixture/error.log"; then tail -40 "$fixture/error.log"; exit 1; fi
printf '%s\n' 'PASS CliReportedGeneratorErrorsBlockInference'
if grep -Fq 'GeneratedIdentity<T1>' "$fixture/success.log"; then tail -40 "$fixture/success.log"; exit 1; fi
printf '%s\n' 'PASS CliGeneratedMethodsExcludedFromSuggestions'
cp "$fixture/success.log" "$evidence/project-generator-success.txt"
cp "$fixture/error.log" "$evidence/project-generator-error.txt"
# Honor an explicit generated-code override without altering the emitted source.
echo ok > "$fixture/Root/mode.txt"
cat > "$fixture/Root/.editorconfig" <<'EOF'
root=true
[GeneratedPlain.cs]
generated_code=false
EOF
dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/generated-override.log" 2>&1 || { tail -40 "$fixture/generated-override.log"; exit 1; }
if ! grep -Fq 'GeneratedIdentity<T1>' "$fixture/generated-override.log"; then tail -40 "$fixture/generated-override.log"; exit 1; fi
rm -- "$fixture/Root/.editorconfig"
printf '%s\n' 'PASS CliGeneratedCodeConfigurationOverride'
echo throw > "$fixture/Root/mode.txt"
dotnet build "$fixture/Root/Root.csproj" --no-restore -p:UseSharedCompilation=false > "$fixture/native-throw.log" 2>&1 || { tail -35 "$fixture/native-throw.log"; exit 1; }
if ! grep -Fq CS8785 "$fixture/native-throw.log"; then tail -35 "$fixture/native-throw.log"; exit 1; fi
if dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/throw.log" 2>&1; then tail -40 "$fixture/throw.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq 'Execution fixture failure' "$fixture/throw.log" || grep -Fq HM0001 "$fixture/throw.log"; then tail -40 "$fixture/throw.log"; exit 1; fi
printf '%s\n' 'PASS CliGeneratorExecutionFailuresBlockPartialInference'
echo ok > "$fixture/Root/mode.txt"
if HM_GENERATOR_INIT_FAIL=1 dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/initialize.log" 2>&1; then tail -40 "$fixture/initialize.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq 'Initialization fixture failure' "$fixture/initialize.log" || grep -Fq HM0001 "$fixture/initialize.log"; then tail -40 "$fixture/initialize.log"; exit 1; fi
printf '%s\n' 'PASS CliGeneratorInitializationFailuresBlockInference'
if HM_GENERATOR_CTOR_FAIL=1 dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/constructor.log" 2>&1; then tail -40 "$fixture/constructor.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq 'Generator loading failed' "$fixture/constructor.log" || grep -Fq HM0001 "$fixture/constructor.log"; then tail -40 "$fixture/constructor.log"; exit 1; fi
printf '%s\n' 'PASS CliGeneratorConstructionFailuresBlockInference'
echo incremental-throw > "$fixture/Root/mode.txt"
if dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/incremental.log" 2>&1; then tail -40 "$fixture/incremental.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq 'Incremental fixture failure' "$fixture/incremental.log" || grep -Fq HM0001 "$fixture/incremental.log"; then tail -40 "$fixture/incremental.log"; exit 1; fi
printf '%s\n' 'PASS CliIncrementalGeneratorFailuresBlockInference'
echo warn > "$fixture/Root/mode.txt"
dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/warning.log" 2>&1 || { tail -40 "$fixture/warning.log"; exit 1; }
if ! grep -Fq HMG002 "$fixture/warning.log" || ! grep -Fq 'Identity<T1>' "$fixture/warning.log"; then tail -40 "$fixture/warning.log"; exit 1; fi
printf '%s\n' 'PASS CliGeneratorWarningsPreserveInference'
if dotnet build "$fixture/Root/Root.csproj" --no-restore -p:WarningsAsErrors=HMG002 > "$fixture/native-promoted.log" 2>&1; then tail -35 "$fixture/native-promoted.log"; exit 1; fi
if ! grep -Fq 'error HMG002' "$fixture/native-promoted.log"; then tail -35 "$fixture/native-promoted.log"; exit 1; fi
if dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore --property WarningsAsErrors=HMG002 > "$fixture/promoted.log" 2>&1; then tail -40 "$fixture/promoted.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq 'error HMG002' "$fixture/promoted.log" || grep -Fq HM0001 "$fixture/promoted.log"; then tail -40 "$fixture/promoted.log"; exit 1; fi
printf '%s\n' 'PASS CliGeneratorWarningsAsErrors'
dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore --property NoWarn=HMG002 > "$fixture/suppressed.log" 2>&1 || { tail -40 "$fixture/suppressed.log"; exit 1; }
if grep -Fq HMG002 "$fixture/suppressed.log" || ! grep -Fq 'Identity<T1>' "$fixture/suppressed.log"; then tail -40 "$fixture/suppressed.log"; exit 1; fi
printf '%s\n' 'PASS CliGeneratorWarningSuppression'
echo generated-warning > "$fixture/Root/mode.txt"
cat > "$fixture/Root/.editorconfig" <<'EOF'
root=true
[GeneratedWarning.cs]
dotnet_diagnostic.CS8603.severity=error
EOF
# Native compiler severity and workspace editor severity are separate policies.
dotnet build "$fixture/Root/Root.csproj" --no-restore > "$fixture/native-generated-error.log" 2>&1 || { tail -35 "$fixture/native-generated-error.log"; exit 1; }
if ! grep -Fq CS8603 "$fixture/native-generated-error.log"; then tail -35 "$fixture/native-generated-error.log"; exit 1; fi
if dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/generated-error.log" 2>&1; then tail -40 "$fixture/generated-error.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq 'error CS8603' "$fixture/generated-error.log" || grep -Fq HM0001 "$fixture/generated-error.log"; then tail -40 "$fixture/generated-error.log"; exit 1; fi
printf '%s\n' 'PASS CliGeneratedSourcePerFileCompilerSeverity'
mkdir -p "$fixture/VisualBasic" "$fixture/VbConsumer"
cat > "$fixture/VisualBasic/VisualBasic.vbproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><RootNamespace>FixtureVb</RootNamespace></PropertyGroup><ItemGroup><Analyzer Include="../Generator/bin/Debug/net10.0/Generator.dll"/><AdditionalFiles Include="mode.txt"/></ItemGroup></Project>
EOF
cat > "$fixture/VisualBasic/Api.vb" <<'EOF'
Public Class VbApi
    Public Shared Function Id(Of T)(value As T) As T
        Return GeneratedContract.Id(value)
    End Function
End Class
EOF
cat > "$fixture/VbConsumer/VbConsumer.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include="../VisualBasic/VisualBasic.vbproj"/></ItemGroup></Project>
EOF
echo 'public static class VbConsumer {public static int Identity(int value)=>FixtureVb.VbApi.Id(value);}' > "$fixture/VbConsumer/Input.cs"
echo ok > "$fixture/VisualBasic/mode.txt"
dotnet build "$fixture/VbConsumer/VbConsumer.csproj" > "$fixture/native-vb.log" 2>&1 || { tail -35 "$fixture/native-vb.log"; exit 1; }
dotnet "$output" --project "$fixture/VbConsumer/VbConsumer.csproj" --no-restore > "$fixture/vb.log" 2>&1 || { tail -40 "$fixture/vb.log"; exit 1; }
if ! grep -Fq 'Identity<T1>' "$fixture/vb.log"; then tail -40 "$fixture/vb.log"; exit 1; fi
printf '%s\n' 'PASS CliVisualBasicGeneratedReferenceMetadata'
echo error > "$fixture/VisualBasic/mode.txt"
if dotnet "$output" --project "$fixture/VbConsumer/VbConsumer.csproj" --no-restore > "$fixture/vb-error.log" 2>&1; then tail -40 "$fixture/vb-error.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq HMG001 "$fixture/vb-error.log" || grep -Fq HM0001 "$fixture/vb-error.log"; then tail -40 "$fixture/vb-error.log"; exit 1; fi
printf '%s\n' 'PASS CliVisualBasicGeneratorErrorsBlockConsumers'
cp "$fixture/promoted.log" "$evidence/project-generator-promoted.txt"
cp "$fixture/throw.log" "$evidence/project-generator-exception.txt"
cp "$fixture/vb-error.log" "$evidence/project-generator-vb-error.txt"
mkdir -p "$fixture/MissingAnalyzer"
cat > "$fixture/MissingAnalyzer/MissingAnalyzer.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><Analyzer Include="MissingGenerator.dll"/></ItemGroup></Project>
EOF
echo 'public static class MissingAnalyzer {public static int Identity(int value)=>value;}' > "$fixture/MissingAnalyzer/Input.cs"
if dotnet "$output" --project "$fixture/MissingAnalyzer/MissingAnalyzer.csproj" > "$fixture/missing.log" 2>&1; then tail -40 "$fixture/missing.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq MissingGenerator.dll "$fixture/missing.log" || grep -Fq HM0001 "$fixture/missing.log"; then tail -40 "$fixture/missing.log"; exit 1; fi
printf '%s\n' 'PASS CliMissingGeneratorReferencesBlockInference'
printf '%s\n' 'Not a managed assembly' > "$fixture/MissingAnalyzer/MissingGenerator.dll"
if dotnet "$output" --project "$fixture/MissingAnalyzer/MissingAnalyzer.csproj" --no-restore > "$fixture/invalid.log" 2>&1; then tail -40 "$fixture/invalid.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Eq 'Generator loading failed|MSBuild loading failed' "$fixture/invalid.log" || grep -Fq HM0001 "$fixture/invalid.log"; then tail -40 "$fixture/invalid.log"; exit 1; fi
printf '%s\n' 'PASS CliInvalidGeneratorAssembliesBlockInference'
echo postinit-warning > "$fixture/Root/mode.txt"
cat > "$fixture/Root/.editorconfig" <<'EOF'
root=true
[PostInitialization.cs]
dotnet_diagnostic.HMG003.severity=error
EOF
if dotnet "$output" --project "$fixture/Root/Root.csproj" --no-restore > "$fixture/postinit-location.log" 2>&1; then tail -40 "$fixture/postinit-location.log"; exit 1; else test "$?" -eq 1; fi
if ! grep -Fq 'error HMG003' "$fixture/postinit-location.log" || grep -Fq HM0001 "$fixture/postinit-location.log"; then tail -40 "$fixture/postinit-location.log"; exit 1; fi
printf '%s\n' 'PASS CliPostInitGeneratorDiagnosticEditorSeverity'
cp "$fixture/postinit-location.log" "$evidence/project-generator-postinit-diagnostic.txt"
